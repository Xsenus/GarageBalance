import type { ReactNode } from 'react'

type FormFieldProps = {
  label: string
  hint?: string
  children: ReactNode
  className?: string
}

export function FormField({ label, hint, children, className }: FormFieldProps) {
  return (
    <label className={`form-field${className ? ` ${className}` : ''}`}>
      <span className="form-field-label">{label}</span>
      {children}
      {hint ? <span className="form-field-hint">{hint}</span> : null}
    </label>
  )
}
