import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'

const monthNames = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь', 'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь']
const weekDayNames = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']

export function LocalizedDatePicker({
  ariaLabel,
  value,
  mode,
  disabled = false,
  onChange,
}: {
  ariaLabel: string
  value: string
  mode: 'date' | 'month'
  disabled?: boolean
  onChange: (value: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [pickerState, setPickerState] = useState(() => ({
    sourceValue: value,
    sourceMode: mode,
    draft: formatLocalizedValue(value, mode),
    viewDate: parseIsoValue(value, mode) ?? new Date(),
  }))
  const rootRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const sourceChanged = pickerState.sourceValue !== value || pickerState.sourceMode !== mode
  const synchronizedState = sourceChanged
    ? {
        sourceValue: value,
        sourceMode: mode,
        draft: formatLocalizedValue(value, mode),
        viewDate: parseIsoValue(value, mode) ?? new Date(),
      }
    : pickerState
  if (sourceChanged) setPickerState(synchronizedState)
  const { draft, viewDate } = synchronizedState
  const effectiveOpen = open && !disabled
  const expectedDraftLength = mode === 'date' ? 10 : 7
  const draftIsInvalid = draft.length >= expectedDraftLength && parseLocalizedValue(draft, mode) === null

  useEffect(() => {
    if (!effectiveOpen) return
    const closeOnOutsidePointer = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', closeOnOutsidePointer)
    return () => document.removeEventListener('mousedown', closeOnOutsidePointer)
  }, [effectiveOpen])

  useEffect(() => {
    if (!effectiveOpen) return
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      setOpen(false)
      triggerRef.current?.focus()
    }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [effectiveOpen])

  const days = useMemo(() => {
    if (mode === 'month') return []
    const year = viewDate.getFullYear()
    const month = viewDate.getMonth()
    const firstWeekDay = (new Date(year, month, 1).getDay() + 6) % 7
    const daysInMonth = new Date(year, month + 1, 0).getDate()
    return [...Array(firstWeekDay).fill(null), ...Array.from({ length: daysInMonth }, (_, index) => index + 1)] as Array<number | null>
  }, [mode, viewDate])

  function commitDraft(nextDraft: string) {
    setPickerState((current) => ({ ...current, sourceValue: value, sourceMode: mode, draft: nextDraft }))
    if (!nextDraft.trim()) {
      onChange('')
      return
    }
    const parsed = parseLocalizedValue(nextDraft, mode)
    if (parsed) {
      onChange(parsed)
      setPickerState({ sourceValue: parsed, sourceMode: mode, draft: nextDraft, viewDate: parseIsoValue(parsed, mode) ?? new Date() })
    }
  }

  function selectMonth(monthIndex: number) {
    const nextValue = `${viewDate.getFullYear()}-${String(monthIndex + 1).padStart(2, '0')}`
    onChange(nextValue)
    setPickerState({ sourceValue: nextValue, sourceMode: mode, draft: formatLocalizedValue(nextValue, mode), viewDate: new Date(viewDate.getFullYear(), monthIndex, 1) })
    setOpen(false)
  }

  function selectDay(day: number) {
    const nextValue = `${viewDate.getFullYear()}-${String(viewDate.getMonth() + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
    onChange(nextValue)
    setPickerState({ sourceValue: nextValue, sourceMode: mode, draft: formatLocalizedValue(nextValue, mode), viewDate: new Date(viewDate.getFullYear(), viewDate.getMonth(), day) })
    setOpen(false)
  }

  function selectCurrentPeriod() {
    const today = new Date()
    const nextValue = mode === 'date'
      ? `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`
      : `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}`
    onChange(nextValue)
    setPickerState({ sourceValue: nextValue, sourceMode: mode, draft: formatLocalizedValue(nextValue, mode), viewDate: new Date(today.getFullYear(), today.getMonth(), mode === 'date' ? today.getDate() : 1) })
    setOpen(false)
  }

  return (
    <div className="localized-date-picker" ref={rootRef}>
      <input
        aria-label={ariaLabel}
        inputMode="numeric"
        maxLength={expectedDraftLength}
        placeholder={mode === 'date' ? 'дд.мм.гггг' : 'мм.гггг'}
        value={draft}
        disabled={disabled}
        aria-invalid={draftIsInvalid}
        onChange={(event) => commitDraft(event.target.value)}
        onBlur={() => setPickerState((current) => ({ ...current, sourceValue: value, sourceMode: mode, draft: formatLocalizedValue(value, mode) }))}
      />
      <button
        ref={triggerRef}
        className="localized-date-picker__trigger"
        type="button"
        aria-label={`Открыть календарь: ${ariaLabel}`}
        aria-expanded={effectiveOpen}
        aria-haspopup="dialog"
        disabled={disabled}
        onClick={() => {
          const parsed = parseIsoValue(value, mode)
          if (parsed) setPickerState((current) => ({ ...current, sourceValue: value, sourceMode: mode, viewDate: parsed }))
          setOpen((current) => !current)
        }}
      >
        <CalendarDays size={17} aria-hidden="true" />
      </button>
      {effectiveOpen ? (
        <div className="localized-date-picker__popover" role="dialog" aria-label={`${ariaLabel}: календарь`}>
          <div className="localized-date-picker__heading">
            <button type="button" aria-label={mode === 'date' ? 'Предыдущий месяц' : 'Предыдущий год'} onClick={() => setPickerState((current) => ({ ...current, viewDate: new Date(viewDate.getFullYear() - (mode === 'month' ? 1 : 0), viewDate.getMonth() - (mode === 'date' ? 1 : 0), 1) }))}>
              <ChevronLeft size={17} aria-hidden="true" />
            </button>
            <strong>{mode === 'date' ? `${monthNames[viewDate.getMonth()]} ${viewDate.getFullYear()}` : viewDate.getFullYear()}</strong>
            <button type="button" aria-label={mode === 'date' ? 'Следующий месяц' : 'Следующий год'} onClick={() => setPickerState((current) => ({ ...current, viewDate: new Date(viewDate.getFullYear() + (mode === 'month' ? 1 : 0), viewDate.getMonth() + (mode === 'date' ? 1 : 0), 1) }))}>
              <ChevronRight size={17} aria-hidden="true" />
            </button>
          </div>
          {mode === 'month' ? (
            <div className="localized-date-picker__months">
              {monthNames.map((month, index) => (
                <button className={isSelectedMonth(value, viewDate.getFullYear(), index) ? 'is-selected' : ''} type="button" aria-pressed={isSelectedMonth(value, viewDate.getFullYear(), index)} key={month} onClick={() => selectMonth(index)}>{month.slice(0, 3)}</button>
              ))}
            </div>
          ) : (
            <div className="localized-date-picker__calendar">
              {weekDayNames.map((day) => <span key={day}>{day}</span>)}
              {days.map((day, index) => day === null
                ? <i key={`empty-${index}`} />
                : <button className={isSelectedDay(value, viewDate, day) ? 'is-selected' : ''} type="button" aria-pressed={isSelectedDay(value, viewDate, day)} key={day} onClick={() => selectDay(day)}>{day}</button>)}
            </div>
          )}
          <div className="localized-date-picker__actions">
            <button className="localized-date-picker__clear" type="button" onClick={() => { onChange(''); setPickerState((current) => ({ ...current, sourceValue: '', sourceMode: mode, draft: '' })); setOpen(false) }}>Очистить</button>
            <button className="localized-date-picker__current" type="button" onClick={selectCurrentPeriod}>{mode === 'date' ? 'Сегодня' : 'Текущий месяц'}</button>
          </div>
        </div>
      ) : null}
    </div>
  )
}

function parseIsoValue(value: string, mode: 'date' | 'month') {
  const match = mode === 'date' ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(value) : /^(\d{4})-(\d{2})$/.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = mode === 'date' ? Number(match[3]) : 1
  const date = new Date(year, month - 1, day)
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) return null
  return date
}

function formatLocalizedValue(value: string, mode: 'date' | 'month') {
  const match = mode === 'date' ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(value) : /^(\d{4})-(\d{2})$/.exec(value)
  if (!match || !parseIsoValue(value, mode)) return ''
  return mode === 'date' ? `${match[3]}.${match[2]}.${match[1]}` : `${match[2]}.${match[1]}`
}

function parseLocalizedValue(value: string, mode: 'date' | 'month') {
  const match = mode === 'date' ? /^(\d{2})\.(\d{2})\.(\d{4})$/.exec(value) : /^(\d{2})\.(\d{4})$/.exec(value)
  if (!match) return null
  const day = mode === 'date' ? Number(match[1]) : 1
  const month = Number(mode === 'date' ? match[2] : match[1])
  const year = Number(mode === 'date' ? match[3] : match[2])
  const date = new Date(year, month - 1, day)
  if (date.getFullYear() !== year || date.getMonth() !== month - 1 || date.getDate() !== day) return null
  return mode === 'date' ? `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}` : `${year}-${String(month).padStart(2, '0')}`
}

function isSelectedMonth(value: string, year: number, monthIndex: number) {
  return value === `${year}-${String(monthIndex + 1).padStart(2, '0')}`
}

function isSelectedDay(value: string, viewDate: Date, day: number) {
  return value === `${viewDate.getFullYear()}-${String(viewDate.getMonth() + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}
