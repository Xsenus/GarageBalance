import { useId, type ReactNode } from 'react'

type FormFieldProps = {
  label: ReactNode
  hint?: string
  help?: string
  children: ReactNode
  className?: string
}

export function FormField({ label, hint, help, children, className }: FormFieldProps) {
  const helpId = useId()

  return (
    <label className={`form-field${className ? ` ${className}` : ''}`}>
      <span className="form-field-label">
        {help ? (
          <span className="field-label-with-help">
            <span>{label}</span>
            <span
              className="field-help"
              tabIndex={0}
              aria-label={`Справка: ${String(label)}`}
              aria-describedby={helpId}
            >
              <span aria-hidden="true">?</span>
              <span id={helpId} className="field-help__tooltip" role="tooltip">{help}</span>
            </span>
          </span>
        ) : label}
      </span>
      {children}
      {hint && !help ? <span className="form-field-hint">{hint}</span> : null}
    </label>
  )
}
