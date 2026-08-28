import type { ReactNode } from 'react'

export function FormError({ children, id }: { children: ReactNode; id?: string }) {
  return (
    <div className="form-error" id={id} role="alert">
      {children}
    </div>
  )
}

export function FormValidationSummary({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <div className="form-error validation-summary" role="alert" aria-label={title}>
      <strong>{title}</strong>
      <ul>
        {items.map((item) => (
          <li key={item}>{item}</li>
        ))}
      </ul>
    </div>
  )
}
