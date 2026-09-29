import type { InputHTMLAttributes } from 'react'

type DecimalTextInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'inputMode' | 'type'>

// Rates and meter values need to keep their own precision and invalid drafts.
// Currency inputs intentionally use the separate two-decimal MoneyInput controls.
export function DecimalTextInput(props: DecimalTextInputProps) {
  return <input {...props} type="text" inputMode="decimal" />
}
