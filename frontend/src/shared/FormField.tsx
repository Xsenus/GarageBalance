import { useId, type ReactNode } from 'react'

type FormFieldProps = {
  label: ReactNode
  hint?: string
  help?: string
  children: ReactNode
  className?: string
}

export function FormField({ label, hint, help, children, className }: FormFieldProps) {
  return (
    <label className={`form-field${className ? ` ${className}` : ''}`}>
      <span className="form-field-label">
        {help ? (
          <span className="field-label-with-help">
            <span>{label}</span>
            <FieldHelp label={String(label)}>{help}</FieldHelp>
          </span>
        ) : label}
      </span>
      {children}
      {hint && !help ? <span className="form-field-hint">{hint}</span> : null}
    </label>
  )
}

export function FieldHelp({ label, children }: { label: string; children: string }) {
  const helpId = useId()

  return (
    <span
      className="field-help"
      tabIndex={0}
      aria-label={`Справка: ${label}`}
      aria-describedby={helpId}
    >
      <span aria-hidden="true">?</span>
      <span id={helpId} className="field-help__tooltip" role="tooltip">{children}</span>
    </span>
  )
}
