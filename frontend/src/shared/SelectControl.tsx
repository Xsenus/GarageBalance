import { Check, ChevronDown } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'

export type SelectControlOption = {
  value: string
  label: string
}

export function SelectControl({
  'aria-label': ariaLabel,
  value,
  options,
  disabled = false,
  onChange,
}: {
  'aria-label': string
  value: string
  options: SelectControlOption[]
  disabled?: boolean
  onChange: (value: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(0)
  const rootRef = useRef<HTMLDivElement>(null)
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([])
  const typeaheadRef = useRef('')
  const typeaheadTimerRef = useRef<number | null>(null)
  const listboxId = useId()
  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value))
  const selectedOption = options[selectedIndex] ?? options[0]
  const optionIds = options.map((_, index) => `${listboxId}-option-${index}`)
  const effectiveOpen = open && !disabled && options.length > 0
  const safeActiveIndex = Math.min(Math.max(activeIndex, 0), Math.max(options.length - 1, 0))

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
    optionRefs.current[safeActiveIndex]?.scrollIntoView?.({ block: 'nearest' })
  }, [effectiveOpen, safeActiveIndex])

  useEffect(() => () => {
    if (typeaheadTimerRef.current !== null) window.clearTimeout(typeaheadTimerRef.current)
  }, [])

  function openList() {
    if (disabled || options.length === 0) return
    setActiveIndex(selectedIndex)
    setOpen(true)
  }

  function selectOption(index: number) {
    const option = options[index]
    if (!option) return
    onChange(option.value)
    setOpen(false)
  }

  function moveActive(direction: -1 | 1) {
    if (options.length === 0) return
    if (!effectiveOpen) {
      setActiveIndex(Math.min(Math.max(selectedIndex + direction, 0), options.length - 1))
      setOpen(true)
      return
    }
    setActiveIndex((current) => Math.min(Math.max(current + direction, 0), options.length - 1))
  }

  function moveActiveByTypeahead(character: string) {
    const query = `${typeaheadRef.current}${character}`.toLocaleLowerCase('ru-RU')
    typeaheadRef.current = query
    if (typeaheadTimerRef.current !== null) window.clearTimeout(typeaheadTimerRef.current)
    typeaheadTimerRef.current = window.setTimeout(() => {
      typeaheadRef.current = ''
      typeaheadTimerRef.current = null
    }, 500)

    const matchIndex = options.findIndex((option) => option.label.toLocaleLowerCase('ru-RU').startsWith(query))
    if (matchIndex < 0) return
    setActiveIndex(matchIndex)
    setOpen(true)
  }

  return (
    <div className="select-control" ref={rootRef}>
      <button
        className="select-control__trigger"
        type="button"
        role="combobox"
        aria-label={ariaLabel}
        aria-expanded={effectiveOpen}
        aria-controls={listboxId}
        aria-activedescendant={effectiveOpen && optionIds[safeActiveIndex] ? optionIds[safeActiveIndex] : undefined}
        disabled={disabled || options.length === 0}
        onClick={() => effectiveOpen ? setOpen(false) : openList()}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setOpen(false)
            return
          }
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault()
            moveActive(event.key === 'ArrowDown' ? 1 : -1)
            return
          }
          if (effectiveOpen && (event.key === 'Home' || event.key === 'End')) {
            event.preventDefault()
            setActiveIndex(event.key === 'Home' ? 0 : options.length - 1)
            return
          }
          if ((event.key === 'Enter' || event.key === ' ') && effectiveOpen) {
            event.preventDefault()
            selectOption(safeActiveIndex)
            return
          }
          if ((event.key === 'Enter' || event.key === ' ') && !effectiveOpen) {
            event.preventDefault()
            openList()
            return
          }
          if (event.key === 'Tab') {
            setOpen(false)
            return
          }
          if (event.key.length === 1 && !event.altKey && !event.ctrlKey && !event.metaKey) {
            moveActiveByTypeahead(event.key)
          }
        }}
      >
        <span>{selectedOption?.label ?? ''}</span>
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      {effectiveOpen ? (
        <div className="select-control__list" id={listboxId} role="listbox" aria-label={`${ariaLabel}: варианты`}>
          {options.map((option, index) => (
            <button
              className={index === safeActiveIndex ? 'select-control__option is-active' : 'select-control__option'}
              id={optionIds[index]}
              key={`${option.value}-${index}`}
              ref={(node) => { optionRefs.current[index] = node }}
              type="button"
              role="option"
              tabIndex={-1}
              aria-selected={option.value === value}
              onMouseEnter={() => setActiveIndex(index)}
              onClick={() => selectOption(index)}
            >
              <span>{option.label}</span>
              {option.value === value ? <Check size={15} aria-hidden="true" /> : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}
