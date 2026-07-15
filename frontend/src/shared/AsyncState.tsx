import { Component } from 'react'
import type { CSSProperties, ErrorInfo, ReactNode } from 'react'

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

export function TableLoadingState({ label, className = '', rows, columns = 4 }: { label: string; className?: string; rows?: number; columns?: number }) {
  const skeletonRows = rows ?? (className.includes('table-loading-state--compact') ? 2 : 4)
  return <LoadingSkeleton className={`table-loading-state ${className}`.trim()} label={label} rows={skeletonRows} columns={columns} />
}

export function EmptyState({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <p className={`empty-state empty-state--spacious ${className}`.trim()} role="status" aria-live="polite">
      {children}
    </p>
  )
}

type AsyncErrorBoundaryProps = {
  children: ReactNode
  fallback: (error: Error, reset: () => void, errorId: string) => ReactNode
  onError?: (error: Error, info: ErrorInfo, errorId: string) => void
}

type AsyncErrorBoundaryState = {
  error: Error | null
  errorId: string | null
}

export class AsyncErrorBoundary extends Component<AsyncErrorBoundaryProps, AsyncErrorBoundaryState> {
  state: AsyncErrorBoundaryState = { error: null, errorId: null }

  static getDerivedStateFromError(error: Error): AsyncErrorBoundaryState {
    const errorId = typeof globalThis.crypto?.randomUUID === 'function'
      ? globalThis.crypto.randomUUID()
      : `client-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
    return { error, errorId }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    this.props.onError?.(error, info, this.state.errorId ?? 'client-error')
  }

  reset = () => {
    this.setState({ error: null, errorId: null })
  }

  render() {
    if (this.state.error) return this.props.fallback(this.state.error, this.reset, this.state.errorId ?? 'client-error')
    return this.props.children
  }
}
