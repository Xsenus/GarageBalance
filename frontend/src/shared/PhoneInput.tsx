import type { InputHTMLAttributes } from 'react'
import { formatRussianPhoneInput, russianPhonePlaceholder } from './phoneNumber'

type PhoneInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'onChange' | 'type' | 'value'> & {
  value: string | null | undefined
  onValueChange: (value: string) => void
}

export function PhoneInput({ value, onValueChange, placeholder = russianPhonePlaceholder, required, ...inputProps }: PhoneInputProps) {
  const formattedValue = formatRussianPhoneInput(value)
  return (
    <input
      {...inputProps}
      type="tel"
      inputMode="tel"
      autoComplete="tel"
      maxLength={18}
      placeholder={placeholder}
      required={required}
      aria-invalid={inputProps['aria-invalid'] ?? (required && formattedValue.length < 18 || undefined)}
      value={formattedValue}
      onChange={(event) => onValueChange(formatRussianPhoneInput(event.target.value))}
    />
  )
}
