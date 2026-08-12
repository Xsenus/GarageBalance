import { Check, ChevronDown } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'

import type { SelectControlOption } from './SelectControl'
import { editableComboboxValuesMatch, findEditableComboboxMatch } from './editableComboboxMatching'

export function EditableCombobox({
  'aria-label': ariaLabel,
  value,
  options,
  disabled = false,
  maxLength,
  placement = 'below',
  onChange,
}: {
  'aria-label': string
  value: string
  options: SelectControlOption[]
  disabled?: boolean
  maxLength?: number
  placement?: 'above' | 'below'
  onChange: (value: string) => void
}) {
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(-1)
  const rootRef = useRef<HTMLDivElement>(null)
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([])
  const listboxId = useId()
  const optionIds = options.map((_, index) => `${listboxId}-option-${index}`)
  const effectiveOpen = open && !disabled && options.length > 0

  useEffect(() => {
    if (!effectiveOpen) return
    const closeOnOutsidePointer = (event: MouseEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', closeOnOutsidePointer, true)
    return () => document.removeEventListener('mousedown', closeOnOutsidePointer, true)
  }, [effectiveOpen])

  useEffect(() => {
    if (!effectiveOpen || activeIndex < 0) return
    optionRefs.current[activeIndex]?.scrollIntoView?.({ block: 'nearest' })
  }, [activeIndex, effectiveOpen])

  function openList(nextValue = value) {
    if (disabled || options.length === 0) return
    setActiveIndex(findEditableComboboxMatch(options, nextValue))
    setOpen(true)
  }

  function changeValue(nextValue: string) {
    onChange(nextValue)
    setActiveIndex(findEditableComboboxMatch(options, nextValue))
    setOpen(true)
  }

  function selectOption(index: number) {
    const option = options[index]
    if (!option) return
    onChange(option.value)
    setActiveIndex(index)
    setOpen(false)
  }

  function moveActive(direction: -1 | 1) {
    if (options.length === 0) return
    if (!effectiveOpen) {
      openList()
      return
    }
    setActiveIndex((current) => {
      if (current < 0) return direction === 1 ? 0 : options.length - 1
      return Math.min(Math.max(current + direction, 0), options.length - 1)
    })
  }

  return (
    <div className="editable-combobox" ref={rootRef}>
      <input
        className="editable-combobox__input"
        type="text"
        role="combobox"
        aria-label={ariaLabel}
        aria-autocomplete="list"
        aria-expanded={effectiveOpen}
        aria-controls={listboxId}
        aria-activedescendant={effectiveOpen && activeIndex >= 0 ? optionIds[activeIndex] : undefined}
        autoComplete="off"
        disabled={disabled}
        maxLength={maxLength}
        value={value}
        onClick={() => openList()}
        onChange={(event) => changeValue(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            if (effectiveOpen) {
              event.preventDefault()
              event.stopPropagation()
              setOpen(false)
            }
            return
          }
          if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault()
            moveActive(event.key === 'ArrowDown' ? 1 : -1)
            return
          }
          if (effectiveOpen && event.key === 'Home') {
            event.preventDefault()
            setActiveIndex(0)
            return
          }
          if (effectiveOpen && event.key === 'End') {
            event.preventDefault()
            setActiveIndex(options.length - 1)
            return
          }
          if (effectiveOpen && event.key === 'Enter' && activeIndex >= 0) {
            event.preventDefault()
            selectOption(activeIndex)
            return
          }
          if (event.key === 'Tab') setOpen(false)
        }}
      />
      <button
        className="editable-combobox__trigger"
        type="button"
        aria-label={`${ariaLabel}: открыть список`}
        aria-expanded={effectiveOpen}
        disabled={disabled || options.length === 0}
        onMouseDown={(event) => event.preventDefault()}
        onClick={() => effectiveOpen ? setOpen(false) : openList()}
      >
        <ChevronDown size={16} aria-hidden="true" />
      </button>
      {effectiveOpen ? (
        <div
          className={`select-control__list editable-combobox__list${placement === 'above' ? ' select-control__list--above' : ''}`}
          id={listboxId}
          role="listbox"
          aria-label={`${ariaLabel}: варианты`}
        >
          {options.map((option, index) => (
            <button
              className={index === activeIndex ? 'select-control__option is-active' : 'select-control__option'}
              id={optionIds[index]}
              key={`${option.value}-${index}`}
              ref={(node) => { optionRefs.current[index] = node }}
              type="button"
              role="option"
              tabIndex={-1}
              aria-selected={editableComboboxValuesMatch(option.value, value)}
              onMouseEnter={() => setActiveIndex(index)}
              onClick={() => selectOption(index)}
            >
              <span>{option.label}</span>
              {editableComboboxValuesMatch(option.value, value) ? <Check size={15} aria-hidden="true" /> : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}
