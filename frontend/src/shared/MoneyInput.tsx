import { useState, type InputHTMLAttributes } from 'react'
import { formatMoneyInput, parseMoneyInput } from './moneyInputFormatting'

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
