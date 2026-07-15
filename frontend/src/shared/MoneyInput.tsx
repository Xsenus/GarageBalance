import { useState, type InputHTMLAttributes } from 'react'
import { formatMoneyInput, formatMoneyTextInput, parseMoneyInput } from './moneyInputFormatting'

type MoneyInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'value' | 'onChange'> & {
  value: number
  onValueChange: (value: number) => void
}

export function MoneyInput({ value, onValueChange, onBlur, onFocus, ...inputProps }: MoneyInputProps) {
  const [draft, setDraft] = useState(() => formatMoneyInput(value))
  const [focused, setFocused] = useState(false)

  return (
    <input
      {...inputProps}
      type="text"
      inputMode="decimal"
      placeholder="0.00"
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
        const parsedValue = parseMoneyInput(draft)
        setFocused(false)
        setDraft(formatMoneyInput(parsedValue))
        onValueChange(parsedValue)
        onBlur?.(event)
      }}
    />
  )
}

type MoneyTextInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'value' | 'onChange'> & {
  value: string
  onValueChange: (value: string) => void
}

export function MoneyTextInput({ value, onValueChange, onBlur, onFocus, ...inputProps }: MoneyTextInputProps) {
  const [focused, setFocused] = useState(false)

  return (
    <input
      {...inputProps}
      type="text"
      inputMode="decimal"
      placeholder="0.00"
      value={focused ? value : formatMoneyTextInput(value)}
      onFocus={(event) => {
        onValueChange(formatMoneyTextInput(value))
        setFocused(true)
        onFocus?.(event)
      }}
      onChange={(event) => onValueChange(event.target.value)}
      onBlur={(event) => {
        onValueChange(formatMoneyTextInput(value))
        setFocused(false)
        onBlur?.(event)
      }}
    />
  )
}
