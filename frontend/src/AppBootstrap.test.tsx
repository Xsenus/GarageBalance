import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AppBootstrapBoundary, AppBootstrapReady } from './AppBootstrap'
import { getFreshApplicationUrl } from './appBootstrapRuntime'

function BrokenApplication() {
  throw new Error('startup failed')
}

describe('application bootstrap protection', () => {
  it('marks bootstrap as ready only after React commits visible content', async () => {
    const readyListener = vi.fn()
    window.addEventListener('garagebalance:bootstrap-ready', readyListener)

    render(
      <AppBootstrapReady>
        <main>Рабочий экран</main>
      </AppBootstrapReady>,
    )

    expect(screen.getByText('Рабочий экран')).toBeInTheDocument()
    await waitFor(() => expect(readyListener).toHaveBeenCalledTimes(1))
    expect(document.documentElement).toHaveAttribute('data-app-ready', 'true')
    window.removeEventListener('garagebalance:bootstrap-ready', readyListener)
    delete document.documentElement.dataset.appReady
  })

  it('shows a retryable screen instead of a blank page after a root render failure', async () => {
    const readyListener = vi.fn()
    const retry = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    window.addEventListener('garagebalance:bootstrap-ready', readyListener)

    render(
      <AppBootstrapBoundary onRetry={retry}>
        <BrokenApplication />
      </AppBootstrapBoundary>,
    )

    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось запустить GarageBalance')
    fireEvent.click(screen.getByRole('button', { name: 'Обновить приложение' }))
    expect(retry).toHaveBeenCalledTimes(1)
    expect(readyListener).toHaveBeenCalledTimes(1)

    window.removeEventListener('garagebalance:bootstrap-ready', readyListener)
    consoleError.mockRestore()
  })

  it('adds a fresh-load marker without losing the current route or query', () => {
    const result = new URL(getFreshApplicationUrl('https://sgk.blagodaty.ru/contractors?tab=suppliers', 12345))

    expect(result.pathname).toBe('/contractors')
    expect(result.searchParams.get('tab')).toBe('suppliers')
    expect(result.searchParams.get('_gb_reload')).toBe('12345')
  })
})
