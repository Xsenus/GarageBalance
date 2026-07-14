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

export function EmptyState({ children, className = '' }: { children: ReactNode; className?: string }) {
  return (
    <p className={`empty-state empty-state--spacious ${className}`.trim()} role="status" aria-live="polite">
      {children}
    </p>
  )
}

type AsyncErrorBoundaryProps = {
  children: ReactNode
  fallback: (error: Error, reset: () => void) => ReactNode
  onError?: (error: Error, info: ErrorInfo) => void
}

type AsyncErrorBoundaryState = {
  error: Error | null
}

export class AsyncErrorBoundary extends Component<AsyncErrorBoundaryProps, AsyncErrorBoundaryState> {
  state: AsyncErrorBoundaryState = { error: null }

  static getDerivedStateFromError(error: Error): AsyncErrorBoundaryState {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    this.props.onError?.(error, info)
  }

  reset = () => {
    this.setState({ error: null })
  }

  render() {
    if (this.state.error) return this.props.fallback(this.state.error, this.reset)
    return this.props.children
  }
}
