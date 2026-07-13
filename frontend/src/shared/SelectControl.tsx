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
  onChange,
}: {
  'aria-label': string
  value: string
  options: SelectControlOption[]
  onChange: (value: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(0)
  const rootRef = useRef<HTMLDivElement>(null)
  const listboxId = useId()
  const selectedIndex = Math.max(0, options.findIndex((option) => option.value === value))
  const selectedOption = options[selectedIndex] ?? options[0]
  const optionIds = options.map((_, index) => `${listboxId}-option-${index}`)

  useEffect(() => {
    if (!open) return
    const closeOnOutsidePointer = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', closeOnOutsidePointer)
    return () => document.removeEventListener('mousedown', closeOnOutsidePointer)
  }, [open])

  function openList() {
    setActiveIndex(selectedIndex)
    setOpen(true)
  }

  function selectOption(index: number) {
    const option = options[index]
    if (!option) return
    onChange(option.value)
    setOpen(false)
  }

  return (
    <div className="select-control" ref={rootRef}>
      <button
        className="select-control__trigger"
        type="button"
        role="combobox"
        aria-label={ariaLabel}
        aria-expanded={open}
        aria-controls={listboxId}
        aria-activedescendant={open ? optionIds[activeIndex] : undefined}
        onClick={() => open ? setOpen(false) : openList()}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setOpen(false)
            return
          }
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault()
            if (!open) openList()
            setActiveIndex((current) => event.key === 'ArrowDown'
              ? Math.min(current + 1, options.length - 1)
              : Math.max(current - 1, 0))
            return
          }
          if ((event.key === 'Enter' || event.key === ' ') && open) {
            event.preventDefault()
            selectOption(activeIndex)
          }
        }}
      >
        <span>{selectedOption?.label ?? ''}</span>
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      {open ? (
        <div className="select-control__list" id={listboxId} role="listbox" aria-label={`${ariaLabel}: варианты`}>
          {options.map((option, index) => (
            <button
              className={index === activeIndex ? 'select-control__option is-active' : 'select-control__option'}
              id={optionIds[index]}
              key={option.value || 'all'}
              type="button"
              role="option"
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
