import { useState, type InputHTMLAttributes } from 'react'
import { formatMoneyInput, formatMoneyTextInput, parseMoneyInput } from './moneyInputFormatting'
import { wasEditableInputCommittedOnEnter } from './prototypeEditing'

type MoneyInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'value' | 'onChange'> & {
  value: number
  onValueChange: (value: number) => void
}

function requiredMoneyInvalid(value: number, props: InputHTMLAttributes<HTMLInputElement>, empty = false) {
  return !!props.required && (empty || !Number.isFinite(value) || value < Number(props.min ?? -Infinity) || value > Number(props.max ?? Infinity))
}

export function MoneyInput({ value, onValueChange, onBlur, onFocus, placeholder = '0.00', ...inputProps }: MoneyInputProps) {
  const [draft, setDraft] = useState(() => formatMoneyInput(value))
  const [focused, setFocused] = useState(false)
  const invalid = requiredMoneyInvalid(value, inputProps)

  return (
    <input
      {...inputProps}
      aria-invalid={inputProps['aria-invalid'] ?? (invalid || undefined)}
      type="text"
      inputMode="decimal"
      placeholder={placeholder}
      value={focused ? draft : formatMoneyInput(value)}
      onFocus={(event) => {
        setDraft(formatMoneyInput(value))
        setFocused(true)
        onFocus?.(event)
      }}
      onChange={(event) => {
        const nextDraft = event.target.value
        setDraft(nextDraft)
        onValueChange(parseMoneyInput(nextDraft))
      }}
      onBlur={(event) => {
        if (!wasEditableInputCommittedOnEnter(event.currentTarget)) {
          const parsedValue = parseMoneyInput(draft)
          setFocused(false)
          setDraft(formatMoneyInput(parsedValue))
          onValueChange(parsedValue)
        }
        onBlur?.(event)
      }}
    />
  )
}

type MoneyTextInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'value' | 'onChange'> & {
  value: string
  onValueChange: (value: string) => void
}

export function MoneyTextInput({ value, onValueChange, onBlur, onFocus, placeholder = '0.00', ...inputProps }: MoneyTextInputProps) {
  const [focused, setFocused] = useState(false)
  const invalid = requiredMoneyInvalid(Number(value.replace(',', '.').replace(/\s+/g, '')), inputProps, !value.trim())

  return (
    <input
      {...inputProps}
      aria-invalid={inputProps['aria-invalid'] ?? (invalid || undefined)}
      type="text"
      inputMode="decimal"
      placeholder={placeholder}
      value={focused ? value : formatMoneyTextInput(value)}
      onFocus={(event) => {
        onValueChange(formatMoneyTextInput(value))
        setFocused(true)
        onFocus?.(event)
      }}
      onChange={(event) => onValueChange(event.target.value)}
      onBlur={(event) => {
        if (!wasEditableInputCommittedOnEnter(event.currentTarget)) {
          onValueChange(formatMoneyTextInput(value))
          setFocused(false)
        }
        onBlur?.(event)
      }}
    />
  )
}
