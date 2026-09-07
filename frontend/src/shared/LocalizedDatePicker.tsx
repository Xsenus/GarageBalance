import { CalendarDays, ChevronLeft, ChevronRight } from 'lucide-react'
import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useCloseOnOutsidePointer } from './focusHooks'
import { datePickerPosition } from './datePickerPosition'

const monthNames = ['Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь', 'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь']
const weekDayNames = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']

export function LocalizedDatePicker({
  ariaLabel,
  value,
  mode,
  placement = 'below',
  disabled = false,
  required = false,
  'aria-invalid': invalid = false,
  'aria-describedby': ariaDescribedBy,
  onChange,
}: {
  ariaLabel: string
  value: string
  mode: 'date' | 'month'
  placement?: 'above' | 'below'
  disabled?: boolean
  required?: boolean
  'aria-invalid'?: boolean
  'aria-describedby'?: string
  onChange: (value: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [pickerState, setPickerState] = useState(() => ({
    sourceValue: value,
    sourceMode: mode,
    draft: formatLocalizedValue(value, mode),
    viewDate: parseIsoValue(value, mode) ?? new Date(),
  }))
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
  const rootRef = useCloseOnOutsidePointer<HTMLDivElement>(effectiveOpen, setOpen)
  const popoverRef = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    if (!effectiveOpen) return
    const anchor = rootRef.current
    const popover = popoverRef.current
    if (!anchor || !popover) return
    const viewport = window.visualViewport
    const updatePosition = () => {
      const bounds = anchor.getBoundingClientRect()
      const position = datePickerPosition(bounds, popover.scrollHeight + 2, {
        width: viewport?.width ?? window.innerWidth,
        height: viewport?.height ?? window.innerHeight,
        left: viewport?.offsetLeft,
        top: viewport?.offsetTop,
      }, placement)
      for (const [key, value] of Object.entries(position)) {
        popover.style.setProperty(key === 'maxHeight' ? 'max-height' : key, `${value}px`)
      }
    }
    updatePosition()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(updatePosition)
    observer?.observe(anchor)
    observer?.observe(popover)
    window.addEventListener('resize', updatePosition)
    window.addEventListener('scroll', updatePosition, true)
    viewport?.addEventListener('resize', updatePosition)
    viewport?.addEventListener('scroll', updatePosition)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', updatePosition)
      window.removeEventListener('scroll', updatePosition, true)
      viewport?.removeEventListener('resize', updatePosition)
      viewport?.removeEventListener('scroll', updatePosition)
    }
  }, [effectiveOpen, placement, rootRef, viewDate])

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

  function closeCalendar() {
    setOpen(false)
    triggerRef.current?.focus()
  }

  function selectDate(date: Date) {
    const nextValue = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}${mode === 'date' ? `-${String(date.getDate()).padStart(2, '0')}` : ''}`
    onChange(nextValue)
    setPickerState({ sourceValue: nextValue, sourceMode: mode, draft: formatLocalizedValue(nextValue, mode), viewDate: date })
    closeCalendar()
  }

  return (
    <div className="localized-date-picker" ref={rootRef} onKeyDown={(event) => {
      if (effectiveOpen && event.key === 'Escape') {
        event.preventDefault()
        event.stopPropagation()
        closeCalendar()
      }
    }}>
      <input
        aria-label={ariaLabel}
        inputMode="numeric"
        maxLength={expectedDraftLength}
        placeholder={mode === 'date' ? 'дд.мм.гггг' : 'мм.гггг'}
        value={draft}
        disabled={disabled}
        required={required}
        aria-invalid={invalid || draftIsInvalid}
        aria-describedby={ariaDescribedBy}
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
        <div ref={popoverRef} className={`localized-date-picker__popover${placement === 'above' ? ' localized-date-picker__popover--above' : ''}`} role="dialog" aria-label={`${ariaLabel}: календарь`}>
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
                <button className={isSelectedMonth(value, viewDate.getFullYear(), index) ? 'is-selected' : ''} type="button" aria-pressed={isSelectedMonth(value, viewDate.getFullYear(), index)} key={month} onClick={() => selectDate(new Date(viewDate.getFullYear(), index, 1))}>{month.slice(0, 3)}</button>
              ))}
            </div>
          ) : (
            <div className="localized-date-picker__calendar">
              {weekDayNames.map((day) => <span key={day}>{day}</span>)}
              {days.map((day, index) => day === null
                ? <i key={`empty-${index}`} />
                : <button className={isSelectedDay(value, viewDate, day) ? 'is-selected' : ''} type="button" aria-pressed={isSelectedDay(value, viewDate, day)} key={day} onClick={() => selectDate(new Date(viewDate.getFullYear(), viewDate.getMonth(), day))}>{day}</button>)}
            </div>
          )}
          <div className="localized-date-picker__actions">
            <button className="localized-date-picker__clear" type="button" onClick={() => { onChange(''); setPickerState((current) => ({ ...current, sourceValue: '', sourceMode: mode, draft: '' })); closeCalendar() }}>Очистить</button>
            <button className="localized-date-picker__current" type="button" onClick={() => selectDate(new Date())}>{mode === 'date' ? 'Сегодня' : 'Текущий месяц'}</button>
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
  return parseIsoValue(value, mode) ? value.split('-').reverse().join('.') : ''
}

function parseLocalizedValue(value: string, mode: 'date' | 'month') {
  const match = mode === 'date' ? /^(\d{2})\.(\d{2})\.(\d{4})$/.exec(value) : /^(\d{2})\.(\d{4})$/.exec(value)
  if (!match) return null
  const iso = match.slice(1).reverse().join('-')
  return parseIsoValue(iso, mode) ? iso : null
}

function isSelectedMonth(value: string, year: number, monthIndex: number) {
  return value === `${year}-${String(monthIndex + 1).padStart(2, '0')}`
}

function isSelectedDay(value: string, viewDate: Date, day: number) {
  return value === `${viewDate.getFullYear()}-${String(viewDate.getMonth() + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}
