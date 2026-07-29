import { Component, type ErrorInfo, type ReactNode, useEffect } from 'react'

import { markBootstrapReady, reloadApplication } from './appBootstrapRuntime'

type AppBootstrapBoundaryProps = {
  children: ReactNode
  onRetry?: () => void
}

type AppBootstrapBoundaryState = {
  failed: boolean
}

export function AppBootstrapReady({ children }: { children: ReactNode }) {
  useEffect(() => {
    markBootstrapReady()
  }, [])

  return children
}

export class AppBootstrapBoundary extends Component<AppBootstrapBoundaryProps, AppBootstrapBoundaryState> {
  state: AppBootstrapBoundaryState = { failed: false }

  static getDerivedStateFromError(): AppBootstrapBoundaryState {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('GarageBalance application failed during startup.', error, info.componentStack)
    markBootstrapReady()
  }

  render() {
    if (this.state.failed) {
      return (
        <div className="app-bootstrap-loader" role="alert">
          <div className="app-bootstrap-loader__card">
            <p className="app-bootstrap-loader__message">Не удалось запустить GarageBalance</p>
            <p className="app-bootstrap-loader__hint">
              Обновите приложение. Рабочие данные, которые уже были сохранены, не изменятся.
            </p>
            <button
              className="app-bootstrap-loader__retry"
              type="button"
              onClick={this.props.onRetry ?? reloadApplication}
            >
              Обновить приложение
            </button>
          </div>
        </div>
      )
    }

    return this.props.children
  }
}
