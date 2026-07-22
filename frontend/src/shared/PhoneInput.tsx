import type { InputHTMLAttributes } from 'react'
import { formatRussianPhoneInput, russianPhonePlaceholder } from './phoneNumber'

type PhoneInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'onChange' | 'type' | 'value'> & {
  value: string | null | undefined
  onValueChange: (value: string) => void
}

export function PhoneInput({ value, onValueChange, placeholder = russianPhonePlaceholder, ...inputProps }: PhoneInputProps) {
  return (
    <input
      {...inputProps}
      type="tel"
      inputMode="tel"
      autoComplete="tel"
      maxLength={18}
      placeholder={placeholder}
      value={formatRussianPhoneInput(value)}
      onChange={(event) => onValueChange(formatRussianPhoneInput(event.target.value))}
    />
  )
}
