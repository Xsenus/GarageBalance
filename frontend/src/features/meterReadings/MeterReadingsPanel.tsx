import { memo, useCallback, useEffect, useState } from 'react'
import { Save, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { DictionaryClient } from '../../services/dictionariesApi'
import type { CreateMeterReadingRequest, FinanceClient, MeterReadingYearGarageDto } from '../../services/financeApi'
import { TableLoadingState } from '../../shared/AsyncState'
import { FormField } from '../../shared/FormField'
import { TablePagination } from '../../shared/TablePagination'
import { getLocalDateInputValue } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { formatPrototypeChangeValue, handleEditableInputKeyDown } from '../../shared/prototypeEditing'
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

const meterReadingTypes = [
  { id: 'electricity', label: 'Электроэнергия', unit: 'кВт' },
  { id: 'water', label: 'Вода', unit: 'м3' },
] as const

const defaultMeterReadingPageSize = 25

type MeterReadingTypeId = typeof meterReadingTypes[number]['id']

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
  garageNumber: string
  monthLabel: string
  meterTypeLabel: string
  unit: string
  previousValue: string
  nextValue: string
}

type MeterReadingMonth = typeof meterReadingMonths[number]

type MeterReadingsTableProps = {
  appliedYear: string
  draftReadings: Record<string, string>
  garages: MeterReadingYearGarageDto[]
  loading: boolean
  meterType: MeterReadingTypeId
  onCommitReading: (garage: MeterReadingYearGarageDto, month: MeterReadingMonth) => void
  onDraftReadingChange: (cellKey: string, value: string) => void
  savedReadings: Record<string, string>
  savingReadingKey: string | null
  selectedMeterType: typeof meterReadingTypes[number]
  yearIsValid: boolean
}

const MeterReadingsTable = memo(function MeterReadingsTable({
  appliedYear,
  draftReadings,
  garages,
  loading,
  meterType,
  onCommitReading,
  onDraftReadingChange,
  savedReadings,
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
              return (
                <span role="cell" key={cellKey}>
                  <input
                    aria-label={`Гараж ${garage.number}, ${month.label}, показание`}
                    disabled={!yearIsValid || savingReadingKey === cellKey}
                    inputMode="decimal"
                    value={draftReadings[cellKey] ?? savedReadings[cellKey] ?? ''}
                    onBlur={() => onCommitReading(garage, month)}
                    onChange={(event) => onDraftReadingChange(cellKey, event.target.value)}
                    onKeyDown={(event) => handleEditableInputKeyDown(event, () => onCommitReading(garage, month))}
                  />
                </span>
              )
            })}
          </div>
        )) : (
          <div className="meter-readings-empty-row" role="row">
            <span role="cell">В справочнике пока нет гаражей</span>
          </div>
        )}
      </div>
    </div>
  )
})

export function MeterReadingsPrototypePanel({ auth, financeClient }: { auth: AuthResponse; dictionaryClient: DictionaryClient; financeClient: FinanceClient }) {
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
  const [savingReadingKey, setSavingReadingKey] = useState<string | null>(null)
  const [pendingReadingChange, setPendingReadingChange] = useState<MeterReadingPrototypePendingChange | null>(null)

  const meterType: MeterReadingTypeId = 'electricity'
  const selectedMeterType = meterReadingTypes[0]
  const yearIsValid = isValidMeterReadingYear(yearDraft)

  function cancelPendingReadingChange() {
    if (pendingReadingChange) {
      setDraftReadings((currentDrafts) => ({
        ...currentDrafts,
        [pendingReadingChange.cellKey]: pendingReadingChange.previousValue,
      }))
    }

    setPendingReadingChange(null)
  }

  function confirmPendingReadingChange() {
    if (!pendingReadingChange) {
      return
    }

    void saveReadingValue(pendingReadingChange.cellKey, pendingReadingChange.readingId, pendingReadingChange.nextValue)
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
  useEscapeKey(Boolean(pendingReadingChange), () => cancelPendingReadingChange())

  useEffect(() => {
    let isMounted = true

    async function loadMeterReadings() {
      setLoading(true)
      setError(null)
      try {
        const yearPage = await financeClient.getMeterReadingYearPage(auth.accessToken, {
          year: Number(appliedYear),
          meterKind: meterType,
          limit: pageSize,
          offset: pageOffset,
        })
        if (!isMounted) {
          return
        }

        const nextSavedReadings: Record<string, string> = {}
        const nextSavedReadingIds: Record<string, string> = {}
        yearPage.readings.forEach((reading) => {
          const monthKey = reading.accountingMonth.slice(5, 7)
          const cellKey = createMeterReadingCellKey(appliedYear, meterType, reading.garageId, monthKey)
          nextSavedReadings[cellKey] = formatMeterReadingInputValue(reading.currentValue)
          nextSavedReadingIds[cellKey] = reading.id
        })

        setGarages(yearPage.garages)
        setTotalGarageCount(yearPage.totalCount)
        setSavedReadings(nextSavedReadings)
        setDraftReadings(nextSavedReadings)
        setSavedReadingIds(nextSavedReadingIds)
        setLoading(false)
      } catch (loadError) {
        if (!isMounted) {
          return
        }

        setError(loadError instanceof Error ? loadError.message : 'Не удалось загрузить гаражи.')
        setGarages([])
        setTotalGarageCount(0)
        setLoading(false)
      }
    }

    void loadMeterReadings()

    return () => {
      isMounted = false
    }
  }, [appliedYear, auth.accessToken, financeClient, meterType, pageOffset, pageSize])

  const saveReadingValue = useCallback(async (cellKey: string, readingId: string | undefined, nextValue: string) => {
    const [, , garageId, monthKey] = cellKey.split(':')
    const parsedValue = parseMeterReadingInputValue(nextValue)
    if (parsedValue === null) {
      setError('Введите показание неотрицательным числом.')
      return
    }

    const request: CreateMeterReadingRequest = {
      garageId,
      meterKind: meterType,
      accountingMonth: `${appliedYear}-${monthKey}-01`,
      readingDate: getLocalDateInputValue(),
      currentValue: parsedValue,
      comment: 'Ввод из годовой таблицы показаний',
    }

    setSavingReadingKey(cellKey)
    setError(null)
    try {
      const savedReading = readingId
        ? await financeClient.updateMeterReading(auth.accessToken, readingId, request)
        : await financeClient.createMeterReading(auth.accessToken, request)
      const savedValue = formatMeterReadingInputValue(savedReading.currentValue)
      setSavedReadings((currentReadings) => ({ ...currentReadings, [cellKey]: savedValue }))
      setDraftReadings((currentDrafts) => ({ ...currentDrafts, [cellKey]: savedValue }))
      setSavedReadingIds((currentIds) => ({ ...currentIds, [cellKey]: savedReading.id }))
      setPendingReadingChange(null)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось сохранить показание.')
    } finally {
      setSavingReadingKey(null)
    }
  }, [appliedYear, auth.accessToken, financeClient, meterType])

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

    if (previousValue.trim() === '') {
      void saveReadingValue(cellKey, undefined, nextValue)
      return
    }

    setPendingReadingChange({
      cellKey,
      readingId: savedReadingIds[cellKey],
      garageNumber: garage.number,
      monthLabel: month.label,
      meterTypeLabel: selectedMeterType.label,
      unit: selectedMeterType.unit,
      previousValue,
      nextValue,
    })
  }, [appliedYear, draftReadings, meterType, savedReadingIds, savedReadings, saveReadingValue, savingReadingKey, selectedMeterType.label, selectedMeterType.unit, yearIsValid])

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
            <div className="meter-readings-control meter-readings-fixed-type" aria-label="Тип показаний">{selectedMeterType.label}, {selectedMeterType.unit}</div>
          </FormField>
        </div>
      </div>

      {!yearIsValid ? <div className="form-error" role="alert">Введите год четырьмя цифрами от 1900 до 9999.</div> : null}
      {error ? <div className="form-error" role="alert">{error}</div> : null}

      <MeterReadingsTable
        appliedYear={appliedYear}
        draftReadings={draftReadings}
        garages={garages}
        loading={loading}
        meterType={meterType}
        onCommitReading={commitReading}
        onDraftReadingChange={changeDraftReading}
        savedReadings={savedReadings}
        savingReadingKey={savingReadingKey}
        selectedMeterType={selectedMeterType}
        yearIsValid={yearIsValid}
      />

      <TablePagination
        ariaLabel="Пагинация показаний"
        totalCount={totalGarageCount}
        offset={pageOffset}
        limit={pageSize}
        visibleCount={garages.length}
        disabled={loading}
        pageSizeLabel="Количество гаражей с показаниями"
        onPageChange={(page) => setPageOffset((page - 1) * pageSize)}
        onPageSizeChange={(limit) => { setPageSize(limit); setPageOffset(0) }}
      />

      {pendingReadingChange ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={cancelPendingReadingChange}>
          <section ref={readingChangeDialogRef} className="detail-dialog contractors-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="meter-reading-change-title" aria-describedby="meter-reading-change-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="meter-reading-change-title">Подтвердить показание?</h3>
                <p>{`Гараж ${pendingReadingChange.garageNumber}, ${pendingReadingChange.monthLabel}`}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Закрыть подтверждение показания" onClick={cancelPendingReadingChange}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="meter-reading-change-description">Проверьте новое показание счетчика. После сохранения backend запишет изменение в историю по гаражу, месяцу и типу счетчика.</p>
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
            <div className="detail-dialog-actions contractors-dialog-actions">
              <button ref={readingChangeCancelRef} className="ghost-button" type="button" onClick={cancelPendingReadingChange}>Отмена</button>
              <button className="secondary-button" type="button" onClick={confirmPendingReadingChange}>
                <Save size={16} />
                <span>Сохранить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}
    </section>
  )
}
