import type { CSSProperties, ReactNode } from 'react'

type LoadingSkeletonProps = {
  label: string
  rows?: number
  columns?: number
  className?: string
}

export function LoadingSkeleton({ label, rows = 3, columns = 1, className = '' }: LoadingSkeletonProps) {
  const style = { '--skeleton-columns': columns } as CSSProperties

  return (
    <div className={`loading-skeleton ${className}`.trim()} role="status" aria-live="polite" aria-label={label} style={style}>
      <span className="visually-hidden">{label}</span>
      {Array.from({ length: rows }, (_, rowIndex) => (
        <div className="loading-skeleton-row" aria-hidden="true" key={rowIndex}>
          {Array.from({ length: columns }, (_, columnIndex) => (
            <span className="loading-skeleton-line" key={columnIndex} />
          ))}
        </div>
      ))}
    </div>
  )
}

export function TableLoadingState({ label, className = '' }: { label: string; className?: string }) {
  return (
    <div className={`table-loading-state ${className}`.trim()} role="status" aria-live="polite" aria-label={label}>
      <span className="table-loading-state-spinner" aria-hidden="true" />
      <span className="table-loading-state-label">{label}</span>
    </div>
  )
}

export function EmptyState({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <p className={`empty-state empty-state--spacious ${className}`.trim()} role="status" aria-live="polite">
      {children}
    </p>
  )
}
