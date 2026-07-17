import type { InputHTMLAttributes } from 'react'

type MeterReadingInputProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'inputMode' | 'type'>

export function MeterReadingInput(props: MeterReadingInputProps) {
  return <input {...props} type="text" inputMode="decimal" />
}
