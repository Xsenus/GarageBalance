import { memo, useCallback, useEffect, useRef, useState } from 'react'
import { LoaderCircle, RefreshCw, Save, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { CreateMeterReadingRequest, FinanceClient, MeterReadingYearGarageDto } from '../../services/financeApi'
import { AsyncErrorState, EmptyState, TableLoadingState } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { LocalizedDatePicker } from '../../shared/LocalizedDatePicker'
import { MeterReadingInput } from '../../shared/MeterReadingInput'
import { SelectControl } from '../../shared/SelectControl'
import { TablePagination } from '../../shared/TablePagination'
import { getLocalDateInputValue } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { formatPrototypeChangeValue, handleEditableInputKeyDown, shouldCommitEditableInputOnBlur } from '../../shared/prototypeEditing'
import { hasPermission, permissions } from '../../shared/accessControl'
import { getMeterReadingDateForMonth } from './meterReadingPeriod'
const meterReadingMonths = [
  { key: '01', label: 'Январь' },
  { key: '02', label: 'Февраль' },
  { key: '03', label: 'Март' },
  { key: '04', label: 'Апрель' },
  { key: '05', label: 'Май' },
  { key: '06', label: 'Июнь' },
  { key: '07', label: 'Июль' },
  { key: '08', label: 'Август' },
  { key: '09', label: 'Сентябрь' },
  { key: '10', label: 'Октябрь' },
  { key: '11', label: 'Ноябрь' },
  { key: '12', label: 'Декабрь' },
]

type MeterReadingTypeId = string
type MeterReadingTypeOption = { id: MeterReadingTypeId; label: string; unit: string }

const meterReadingTypes: MeterReadingTypeOption[] = [
  { id: 'electricity', label: 'Электроэнергия', unit: 'кВт·ч' },
  { id: 'water', label: 'Вода', unit: 'м³' },
]

const defaultMeterReadingPageSize = 25
const emptyMeterReplacementForm = { serial: '', initialValue: '0', finalValue: '', reason: '', date: '' }

function createMeterReadingCellKey(year: string, meterType: MeterReadingTypeId, garageId: string, monthKey: string) {
  return `${year}:${meterType}:${garageId}:${monthKey}`
}

function isValidMeterReadingYear(value: string) {
  if (!/^\d{4}$/.test(value)) {
    return false
  }

  const year = Number(value)
  return year >= 1900 && year <= 9999
}

function formatMeterReadingInputValue(value: number) {
  return Number.isInteger(value) ? String(value) : String(value).replace('.', ',')
}

function parseMeterReadingInputValue(value: string) {
  const normalizedValue = value.trim().replace(/\s/g, '').replace(',', '.')
  if (!/^\d+(\.\d+)?$/.test(normalizedValue)) {
    return null
  }

  const parsedValue = Number(normalizedValue)
  return Number.isFinite(parsedValue) && parsedValue >= 0 ? parsedValue : null
}

type MeterReadingPrototypePendingChange = {
  cellKey: string
  readingId?: string
  readingVersion?: string
  garageNumber: string
  monthLabel: string
  previousValue: string
  nextValue: string
  isOutsideCurrentMonth: boolean
  suggestsReplacement: boolean
}

type MeterReadingMonth = typeof meterReadingMonths[number]

type MeterReadingsTableProps = {
  appliedYear: string
  canEditOutsideCurrentMonth: boolean
  currentMonth: string
  draftReadings: Record<string, string>
  garages: MeterReadingYearGarageDto[]
  loading: boolean
  meterType: MeterReadingTypeId
  onCommitReading: (garage: MeterReadingYearGarageDto, month: MeterReadingMonth) => void
  onDraftReadingChange: (cellKey: string, value: string) => void
  savedReadings: Record<string, string>
  savedReadingReplacements: Record<string, string>
  savingReadingKey: string | null
  selectedMeterType: typeof meterReadingTypes[number]
  yearIsValid: boolean
}

const MeterReadingsTable = memo(function MeterReadingsTable({
  appliedYear,
  canEditOutsideCurrentMonth,
  currentMonth,
  draftReadings,
  garages,
  loading,
  meterType,
  onCommitReading,
  onDraftReadingChange,
  savedReadings,
  savedReadingReplacements,
  savingReadingKey,
  selectedMeterType,
  yearIsValid,
}: MeterReadingsTableProps) {
  return (
    <div className="meter-readings-table-shell">
      <div className="meter-readings-table" role="table" aria-label={`Показания счетчиков за ${appliedYear} год`}>
        <div className="meter-readings-title-row" role="row">
          <span role="columnheader">Гараж</span>
          <span role="columnheader">Показания</span>
        </div>
        <div className="meter-readings-month-row" role="row">
          <span role="columnheader">Гараж</span>
          {meterReadingMonths.map((month) => (
            <span role="columnheader" key={month.key}>
              <strong>{month.label}</strong>
              <small>{selectedMeterType.unit}</small>
            </span>
          ))}
        </div>
        {loading ? (
          <div className="meter-readings-loading-row" role="row">
            <span role="cell">
              <TableLoadingState label="Загружаем гаражи и показания" />
            </span>
          </div>
        ) : garages.length > 0 ? garages.map((garage) => (
          <div className="meter-readings-data-row" role="row" key={garage.id}>
            <span role="rowheader">Гараж {garage.number}</span>
            {meterReadingMonths.map((month) => {
              const cellKey = createMeterReadingCellKey(appliedYear, meterType, garage.id, month.key)
              const futureMonth = `${appliedYear}-${month.key}` > currentMonth
              const replacementSerial = savedReadingReplacements[cellKey]
              return (
                <span className={replacementSerial ? 'meter-readings-value-cell meter-readings-value-cell--replacement' : 'meter-readings-value-cell'} role="cell" key={cellKey}>
                  {replacementSerial ? (
                    <span
                      className="meter-readings-replacement-marker"
                      role="img"
                      aria-label={`С этого месяца установлен новый счетчик ${replacementSerial}`}
                      title={`Замена счетчика. Новый номер: ${replacementSerial}`}
                    >
                      <RefreshCw size={13} aria-hidden="true" />
                    </span>
                  ) : null}
                  <MeterReadingInput
                    aria-label={`Гараж ${garage.number}, ${month.label}, показание`}
                    disabled={!yearIsValid || (futureMonth && !canEditOutsideCurrentMonth) || savingReadingKey === cellKey}
                    value={draftReadings[cellKey] ?? savedReadings[cellKey] ?? ''}
                    onBlur={(event) => {
                      if (shouldCommitEditableInputOnBlur(event.currentTarget)) onCommitReading(garage, month)
                    }}
                    onChange={(event) => onDraftReadingChange(cellKey, event.target.value)}
                    onKeyDown={(event) => handleEditableInputKeyDown(event, () => onCommitReading(garage, month))}
                  />
                </span>
              )
            })}
          </div>
        )) : (
          <div className="meter-readings-empty-row" role="row">
            <span role="cell">В справочнике нет гаражей</span>
          </div>
        )}
      </div>
    </div>
  )
})

export function MeterReadingsPrototypePanel({ auth, dictionaryClient, financeClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient }) {
  const canEditOutsideCurrentMonth = hasPermission(auth, permissions.historicalMeterReadingsCorrect)
  const [yearDraft, setYearDraft] = useState('2026')
  const [appliedYear, setAppliedYear] = useState('2026')
  const [garages, setGarages] = useState<MeterReadingYearGarageDto[]>([])
  const [pageOffset, setPageOffset] = useState(0)
  const [pageSize, setPageSize] = useState(defaultMeterReadingPageSize)
  const [totalGarageCount, setTotalGarageCount] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [savedReadings, setSavedReadings] = useState<Record<string, string>>({})
  const [draftReadings, setDraftReadings] = useState<Record<string, string>>({})
  const [savedReadingIds, setSavedReadingIds] = useState<Record<string, string>>({})
  const [savedReadingVersions, setSavedReadingVersions] = useState<Record<string, string>>({})
  const [savedReadingReplacements, setSavedReadingReplacements] = useState<Record<string, string>>({})
  const [savingReadingKey, setSavingReadingKey] = useState<string | null>(null)
  const [pendingReadingChange, setPendingReadingChange] = useState<MeterReadingPrototypePendingChange | null>(null)
  const [readingChangeError, setReadingChangeError] = useState<string | null>(null)
  const [availableMeterTypes, setAvailableMeterTypes] = useState<MeterReadingTypeOption[] | null>(null)
  const [meterType, setMeterType] = useState<MeterReadingTypeId>('electricity')
  const [reloadRevision, setReloadRevision] = useState(0)
  const backgroundReloadRef = useRef(false)
  const [replacementForm, setReplacementForm] = useState(emptyMeterReplacementForm)

  const selectedMeterType = availableMeterTypes?.find((item) => item.id === meterType)
    ?? meterReadingTypes.find((item) => item.id === meterType)
    ?? meterReadingTypes[0]
  const yearIsValid = isValidMeterReadingYear(yearDraft)
  const [currentMonth, setCurrentMonth] = useState(getLocalDateInputValue().slice(0, 7))
  const pendingReadingSaving = Boolean(pendingReadingChange && savingReadingKey === pendingReadingChange.cellKey)

  function cancelPendingReadingChange() {
    if (pendingReadingSaving) {
      return
    }
    if (pendingReadingChange) {
      setDraftReadings((currentDrafts) => ({
        ...currentDrafts,
        [pendingReadingChange.cellKey]: pendingReadingChange.previousValue,
      }))
    }

    setPendingReadingChange(null)
    setReadingChangeError(null)
    setReplacementForm(emptyMeterReplacementForm)
  }

  function updateReplacementForm(field: keyof typeof emptyMeterReplacementForm, value: string) {
    setReplacementForm((current) => ({ ...current, [field]: value }))
  }

  async function confirmPendingReadingChange() {
    if (!pendingReadingChange || pendingReadingSaving) {
      return
    }

    if (pendingReadingChange.suggestsReplacement) {
      const currentValue = parseMeterReadingInputValue(pendingReadingChange.nextValue)
      const initialValue = parseMeterReadingInputValue(replacementForm.initialValue)
      const finalValue = parseMeterReadingInputValue(replacementForm.finalValue)
      if (!financeClient.replaceMeterDevice || currentValue === null || initialValue === null || finalValue === null || !replacementForm.serial.trim() || !replacementForm.reason.trim() || !replacementForm.date) {
        setReadingChangeError('Заполните все поля замены счетчика.')
        return
      }

      const [, , garageId, monthKey] = pendingReadingChange.cellKey.split(':')
      setSavingReadingKey(pendingReadingChange.cellKey)
      setReadingChangeError(null)
      try {
        await financeClient.replaceMeterDevice(auth.accessToken, {
          garageId,
          meterKind: meterType,
          accountingMonth: `${appliedYear}-${monthKey}-01`,
          replacementDate: replacementForm.date,
          newSerialNumber: replacementForm.serial.trim(),
          newInitialValue: initialValue,
          currentValue,
          removedDeviceFinalValue: finalValue,
          reason: replacementForm.reason.trim(),
          meterReadingId: pendingReadingChange.readingId,
          expectedReadingVersion: pendingReadingChange.readingVersion,
        })
        setPendingReadingChange(null)
        backgroundReloadRef.current = true
        setReloadRevision((revision) => revision + 1)
      } catch (caught) {
        setReadingChangeError(caught instanceof Error ? caught.message : 'Не удалось оформить замену счетчика.')
      } finally {
        setSavingReadingKey(null)
      }
      return
    }

    void saveReadingValue(
      pendingReadingChange.cellKey,
      pendingReadingChange.readingId,
      pendingReadingChange.readingVersion,
      pendingReadingChange.nextValue,
      true,
    )
  }

  function updateYearDraft(value: string) {
    const nextYear = value.replace(/\D/g, '').slice(0, 4)
    setYearDraft(nextYear)

    if (isValidMeterReadingYear(nextYear)) {
      setPageOffset(0)
      setAppliedYear(nextYear)
    }
  }

  function applyYearDraft() {
    if (isValidMeterReadingYear(yearDraft)) {
      setAppliedYear(yearDraft)
    }
  }

  useRestoreFocusOnClose(Boolean(pendingReadingChange))
  const readingChangeDialogRef = useFocusTrap<HTMLElement>(Boolean(pendingReadingChange))
  const readingChangeCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(pendingReadingChange))
  useEscapeKey(Boolean(pendingReadingChange) && !pendingReadingSaving, () => cancelPendingReadingChange())

  useEffect(() => {
    let isMounted = true
    const controller = new AbortController()

    async function loadMeterConfiguration() {
      setError(null)
      try {
        const settings = await dictionaryClient.getChargeServiceSettings(auth.accessToken, undefined, 1000, false, true, true, controller.signal)
        if (!isMounted) {
          return
        }

        const nextTypes = settings
          .filter((setting) => setting.isRegular && setting.isMetered && !setting.isArchived)
          .map((setting) => {
            const legacyType = meterReadingTypes.find((item) =>
              setting.meterKind === item.id || (!setting.meterKind && setting.tariffCalculationBase === `meter_${item.id}`))
            const id = setting.meterKind?.trim() || legacyType?.id
            const unit = setting.unitName?.trim()
            return id ? {
              id,
              label: legacyType?.label ?? setting.name,
              unit: unit && !unit.startsWith('руб') ? unit : legacyType?.unit ?? unit ?? '',
            } : null
          })
          .filter((item): item is MeterReadingTypeOption => item !== null)
        setAvailableMeterTypes(nextTypes)
        setMeterType((currentType) => nextTypes.some((item) => item.id === currentType)
          ? currentType
          : nextTypes[0]?.id ?? 'electricity')
      } catch (loadError) {
        if (!isMounted) {
          return
        }

        setAvailableMeterTypes([])
        setError(loadError instanceof Error ? loadError.message : 'Не удалось загрузить настройки счётчиков.')
      }
    }

    void loadMeterConfiguration()
    return () => {
      isMounted = false
      controller.abort()
    }
  }, [auth.accessToken, dictionaryClient])

  useEffect(() => {
    if (!availableMeterTypes?.some((item) => item.id === meterType)) {
      return
    }

    let isMounted = true
    const controller = new AbortController()
    const background = backgroundReloadRef.current
    backgroundReloadRef.current = false

    async function loadMeterReadings() {
      setLoading(!background)
      setError(null)
      try {
        const yearPage = await financeClient.getMeterReadingYearPage(auth.accessToken, {
          year: Number(appliedYear),
          meterKind: meterType,
          limit: pageSize,
          offset: pageOffset,
        }, controller.signal)
        if (!isMounted) {
          return
        }

        const nextSavedReadings: Record<string, string> = {}
        const nextSavedReadingIds: Record<string, string> = {}
        const nextSavedReadingVersions: Record<string, string> = {}
        const nextSavedReadingReplacements: Record<string, string> = {}
        yearPage.readings.forEach((reading) => {
          const monthKey = reading.accountingMonth.slice(5, 7)
          const cellKey = createMeterReadingCellKey(appliedYear, meterType, reading.garageId, monthKey)
          nextSavedReadings[cellKey] = formatMeterReadingInputValue(reading.currentValue)
          nextSavedReadingIds[cellKey] = reading.id
          nextSavedReadingVersions[cellKey] = reading.version
          if (reading.isMeterReplacement) {
            nextSavedReadingReplacements[cellKey] = reading.meterDeviceSerialNumber?.trim() || 'номер не указан'
          }
        })

        setGarages(yearPage.garages)
        if (yearPage.currentAccountingMonth) {
          setCurrentMonth(yearPage.currentAccountingMonth.slice(0, 7))
        }
        setTotalGarageCount(yearPage.totalCount)
        setSavedReadings(nextSavedReadings)
        setDraftReadings(nextSavedReadings)
        setSavedReadingIds(nextSavedReadingIds)
        setSavedReadingVersions(nextSavedReadingVersions)
        setSavedReadingReplacements(nextSavedReadingReplacements)
      } catch (loadError) {
        if (!isMounted) {
          return
        }

        setError(loadError instanceof Error ? loadError.message : 'Не удалось загрузить гаражи.')
        if (!background) {
          setGarages([])
          setTotalGarageCount(0)
        }
      } finally {
        if (isMounted) setLoading(false)
      }
    }

    void loadMeterReadings()

    return () => {
      isMounted = false
      controller.abort()
    }
  }, [appliedYear, auth.accessToken, availableMeterTypes, financeClient, meterType, pageOffset, pageSize, reloadRevision])

  const saveReadingValue = useCallback(async (
    cellKey: string,
    readingId: string | undefined,
    readingVersion: string | undefined,
    nextValue: string,
    showErrorInDialog = false,
  ) => {
    const [, , garageId, monthKey] = cellKey.split(':')
    const parsedValue = parseMeterReadingInputValue(nextValue)
    if (parsedValue === null) {
      const message = 'Введите показание неотрицательным числом.'
      if (showErrorInDialog) {
        setReadingChangeError(message)
      } else {
        setError(message)
      }
      return
    }

    const request: CreateMeterReadingRequest = {
      garageId,
      meterKind: meterType,
      accountingMonth: `${appliedYear}-${monthKey}-01`,
      readingDate: getMeterReadingDateForMonth(appliedYear, monthKey, currentMonth, getLocalDateInputValue()),
      currentValue: parsedValue,
      comment: 'Ввод из годовой таблицы показаний',
      expectedVersion: readingVersion,
      periodOverrideReason: undefined,
    }

    setSavingReadingKey(cellKey)
    setError(null)
    setReadingChangeError(null)
    try {
      const isHistoricalCorrection = Boolean(readingId && request.accountingMonth.slice(0, 7) !== currentMonth)
      const savedReading = isHistoricalCorrection
        ? await financeClient.correctHistoricalMeterReading!(auth.accessToken, readingId!, {
            readingDate: request.readingDate,
            currentValue: request.currentValue,
            comment: request.comment,
            reason: undefined,
            expectedVersion: readingVersion!,
          })
        : readingId
          ? await financeClient.updateMeterReading(auth.accessToken, readingId, request)
          : await financeClient.createMeterReading(auth.accessToken, request)
      const savedValue = formatMeterReadingInputValue(savedReading.currentValue)
      setSavedReadings((currentReadings) => ({ ...currentReadings, [cellKey]: savedValue }))
      setDraftReadings((currentDrafts) => ({ ...currentDrafts, [cellKey]: savedValue }))
      setSavedReadingIds((currentIds) => ({ ...currentIds, [cellKey]: savedReading.id }))
      setSavedReadingVersions((currentVersions) => ({ ...currentVersions, [cellKey]: savedReading.version }))
      setPendingReadingChange(null)
      setReadingChangeError(null)
      backgroundReloadRef.current = true
      setReloadRevision((revision) => revision + 1)
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить показание.'
      if (showErrorInDialog) {
        setReadingChangeError(message)
      } else {
        setError(message)
      }
    } finally {
      setSavingReadingKey(null)
    }
  }, [appliedYear, auth.accessToken, currentMonth, financeClient, meterType])

  const changeDraftReading = useCallback((cellKey: string, value: string) => {
    setDraftReadings((currentDrafts) => ({ ...currentDrafts, [cellKey]: value }))
  }, [])

  const commitReading = useCallback((garage: MeterReadingYearGarageDto, month: MeterReadingMonth) => {
    if (!yearIsValid) {
      return
    }

    const cellKey = createMeterReadingCellKey(appliedYear, meterType, garage.id, month.key)
    if (savingReadingKey === cellKey) {
      return
    }

    const nextValue = draftReadings[cellKey] ?? ''
    const previousValue = savedReadings[cellKey] ?? ''

    if (nextValue.trim() === previousValue.trim()) {
      return
    }

    const isOutsideCurrentMonth = `${appliedYear}-${month.key}` !== currentMonth
    if (isOutsideCurrentMonth && !canEditOutsideCurrentMonth) {
      setDraftReadings((currentDrafts) => ({ ...currentDrafts, [cellKey]: previousValue }))
      setError('Нет права на показания вне текущего месяца.')
      return
    }

    const parsedNextValue = parseMeterReadingInputValue(nextValue)
    let previousChainValue: string | undefined
    for (let index = Number(month.key) - 1; index >= 1; index -= 1) {
      const previousMonthKey = String(index).padStart(2, '0')
      const candidate = savedReadings[createMeterReadingCellKey(appliedYear, meterType, garage.id, previousMonthKey)]
      if (candidate?.trim()) {
        previousChainValue = candidate
        break
      }
    }
    const parsedPreviousChainValue = previousChainValue ? parseMeterReadingInputValue(previousChainValue) : null
    const suggestsReplacement = parsedNextValue !== null && parsedPreviousChainValue !== null && parsedNextValue < parsedPreviousChainValue

    if (previousValue.trim() === '' && !suggestsReplacement && !isOutsideCurrentMonth) {
      void saveReadingValue(cellKey, undefined, undefined, nextValue)
      return
    }

    const accountingMonth = `${appliedYear}-${month.key}`
    if (isOutsideCurrentMonth && previousValue.trim() !== '' && !suggestsReplacement && (!financeClient.correctHistoricalMeterReading || !savedReadingVersions[cellKey])) {
      setDraftReadings((currentDrafts) => ({ ...currentDrafts, [cellKey]: previousValue }))
      setError('Не удалось подготовить корректировку. Обновите страницу.')
      return
    }

    setPendingReadingChange({
      cellKey,
      readingId: savedReadingIds[cellKey],
      readingVersion: savedReadingVersions[cellKey],
      garageNumber: garage.number,
      monthLabel: month.label,
      previousValue,
      nextValue,
      isOutsideCurrentMonth,
      suggestsReplacement,
    })
    setError(null)
    setReadingChangeError(null)
    setReplacementForm({
      serial: '',
      initialValue: '0',
      finalValue: previousValue.trim() || previousChainValue || '',
      reason: '',
      date: `${accountingMonth}-01`,
    })
  }, [appliedYear, canEditOutsideCurrentMonth, currentMonth, draftReadings, financeClient.correctHistoricalMeterReading, meterType, savedReadingIds, savedReadingVersions, savedReadings, saveReadingValue, savingReadingKey, yearIsValid])

  return (
    <section className="meter-readings-page" aria-label="Показания">
      <div className="meter-readings-heading">
        <div>
          <h1>Показания</h1>
        </div>
        <div className="meter-readings-controls" role="group" aria-label="Параметры показаний">
          <FormField label="Год">
            <input
              aria-label="Год показаний"
              aria-invalid={!yearIsValid}
              className="meter-readings-control"
              inputMode="numeric"
              maxLength={4}
              value={yearDraft}
              onChange={(event) => updateYearDraft(event.target.value)}
              onBlur={applyYearDraft}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  applyYearDraft()
                }
              }}
            />
          </FormField>
          <FormField label="Тип">
            <SelectControl
              aria-label="Тип показаний"
              className="meter-readings-control"
              disabled={!availableMeterTypes || availableMeterTypes.length <= 1}
              value={availableMeterTypes?.some((item) => item.id === meterType) ? meterType : ''}
              options={(availableMeterTypes ?? []).map((item) => ({ value: item.id, label: `${item.label}, ${item.unit}` }))}
              onChange={(value) => {
                setMeterType(value as MeterReadingTypeId)
                setPageOffset(0)
                setPendingReadingChange(null)
                setError(null)
              }}
            />
          </FormField>
        </div>
      </div>

      {!yearIsValid ? <div className="form-error" role="alert">Введите год четырьмя цифрами от 1900 до 9999.</div> : null}
      {error && !pendingReadingChange ? <AsyncErrorState message={error} onRetry={() => setReloadRevision((value) => value + 1)} retrying={loading} /> : null}
      <p className="form-hint">Для другого месяца нужно отдельное право; действие записывается в историю.</p>

      {availableMeterTypes === null ? <TableLoadingState label="Загружаем гаражи и показания" /> : null}
      {availableMeterTypes?.length === 0 && !error ? (
        <EmptyState>Нет действующих услуг по счётчику. Назначьте счётчиковый тариф в разделе «Тарифы и сборы».</EmptyState>
      ) : availableMeterTypes && availableMeterTypes.length > 0 ? <MeterReadingsTable
        appliedYear={appliedYear}
        canEditOutsideCurrentMonth={canEditOutsideCurrentMonth}
        currentMonth={currentMonth}
        draftReadings={draftReadings}
        garages={garages}
        loading={loading}
        meterType={meterType}
        onCommitReading={commitReading}
        onDraftReadingChange={changeDraftReading}
        savedReadings={savedReadings}
        savedReadingReplacements={savedReadingReplacements}
        savingReadingKey={savingReadingKey}
        selectedMeterType={selectedMeterType}
        yearIsValid={yearIsValid}
      /> : null}

      {availableMeterTypes && availableMeterTypes.length > 0 ? <TablePagination
        ariaLabel="Пагинация показаний"
        totalCount={totalGarageCount}
        offset={pageOffset}
        limit={pageSize}
        visibleCount={garages.length}
        disabled={loading}
        pageSizeLabel="Количество гаражей с показаниями"
        onPageChange={(page) => setPageOffset((page - 1) * pageSize)}
        onPageSizeChange={(limit) => { setPageSize(limit); setPageOffset(0) }}
      /> : null}

      {pendingReadingChange ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={cancelPendingReadingChange}>
          <section ref={readingChangeDialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="meter-reading-change-title" aria-describedby="meter-reading-change-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">{pendingReadingChange.suggestsReplacement ? 'Замена счетчика' : pendingReadingChange.isOutsideCurrentMonth ? 'Другой месяц' : 'Изменение'}</p>
                <h3 id="meter-reading-change-title">{pendingReadingChange.suggestsReplacement ? 'Оформить замену счетчика?' : pendingReadingChange.isOutsideCurrentMonth ? 'Сохранить показание за другой месяц?' : 'Подтвердить показание?'}</h3>
                <p>{`Гараж ${pendingReadingChange.garageNumber}, ${pendingReadingChange.monthLabel}`}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение показания" disabled={pendingReadingSaving} onClick={cancelPendingReadingChange}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="meter-reading-change-description">Проверьте показание. После сохранения изменение появится в истории гаража.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля показания">
              <li>
                <span className="dictionary-change-field">Показание</span>
                <span className="dictionary-change-values">
                  <span className="dictionary-change-value">{formatPrototypeChangeValue(pendingReadingChange.previousValue)}</span>
                  <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                  <span className="dictionary-change-value dictionary-change-value-after">{formatPrototypeChangeValue(pendingReadingChange.nextValue)}</span>
                </span>
              </li>
            </ul>
            {pendingReadingChange.suggestsReplacement ? (
              <>
                <div className="form-warning" role="status">
                  Новое показание меньше предыдущего. Укажите замену физического счетчика, чтобы начать отсчет с нового стартового значения.
                </div>
                <div className="contractors-modal-grid">
                <FormField label="Дата замены">
                  <LocalizedDatePicker ariaLabel="Дата замены счетчика" mode="date" value={replacementForm.date} disabled={pendingReadingSaving} onChange={(value) => updateReplacementForm('date', value)} required />
                </FormField>
                <FormField label="Номер нового счетчика">
                  <input aria-label="Номер нового счетчика" maxLength={100} value={replacementForm.serial} disabled={pendingReadingSaving} onChange={(event) => updateReplacementForm('serial', event.target.value)} />
                </FormField>
                <FormField label="Начальное показание нового">
                  <MeterReadingInput aria-label="Начальное показание нового счетчика" value={replacementForm.initialValue} disabled={pendingReadingSaving} onChange={(event) => updateReplacementForm('initialValue', event.target.value)} />
                </FormField>
                <FormField label="Конечное показание старого">
                  <MeterReadingInput aria-label="Конечное показание старого счетчика" value={replacementForm.finalValue} disabled={pendingReadingSaving} onChange={(event) => updateReplacementForm('finalValue', event.target.value)} />
                </FormField>
                <FormField label="Причина замены">
                  <textarea aria-label="Причина замены счетчика" maxLength={500} value={replacementForm.reason} disabled={pendingReadingSaving} onChange={(event) => updateReplacementForm('reason', event.target.value)} />
                </FormField>
                </div>
              </>
            ) : null}
            {pendingReadingChange.isOutsideCurrentMonth && !pendingReadingChange.suggestsReplacement ? (
              <p className="form-hint">Для другого месяца комментарий не требуется: действие будет автоматически записано в историю изменений.</p>
            ) : null}
            {readingChangeError ? <div className="form-error" role="alert">{readingChangeError}</div> : null}
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={readingChangeCancelRef} className="ghost-button" type="button" disabled={pendingReadingSaving} onClick={cancelPendingReadingChange}>Отмена</button>
              <button className="secondary-button" type="button" aria-busy={pendingReadingSaving} disabled={pendingReadingSaving} onClick={confirmPendingReadingChange}>
                {pendingReadingSaving ? <LoaderCircle className="button-spinner" size={16} aria-hidden="true" /> : <Save size={16} />}
                <span>Сохранить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}
