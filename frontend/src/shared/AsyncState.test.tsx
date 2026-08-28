import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AsyncErrorBoundary, AsyncErrorState, BackgroundRefreshStatus, EmptyState, LoadingSkeleton, StatusMessage, TableLoadingState } from './AsyncState'

describe('AsyncState', () => {
  it('announces loading without exposing decorative bars', () => {
    const { container } = render(<LoadingSkeleton label="Загружаем данные" rows={2} columns={3} />)

    expect(screen.getByRole('status', { name: 'Загружаем данные' })).toBeInTheDocument()
    expect(container.querySelectorAll('.loading-skeleton-line')).toHaveLength(6)
    expect(container.querySelector('.loading-skeleton-row')).toHaveAttribute('aria-hidden', 'true')
  })

  it('renders a spacious announced empty state', () => {
    render(<EmptyState>Записей пока нет</EmptyState>)

    expect(screen.getByRole('status')).toHaveTextContent('Записей пока нет')
    expect(screen.getByRole('status')).toHaveClass('empty-state--spacious')
  })

  it('announces a background refresh without replacing loaded content', () => {
    render(<BackgroundRefreshStatus label="Обновляем список" />)

    expect(screen.getByRole('status', { name: 'Обновляем список' })).toHaveTextContent('Обновляем список…')
    expect(screen.getByRole('status')).toHaveClass('form-hint')
  })

  it('renders a compact shared status message', () => {
    render(<StatusMessage>Нет доступных строк</StatusMessage>)

    expect(screen.getByRole('status')).toHaveTextContent('Нет доступных строк')
    expect(screen.getByRole('status')).toHaveClass('empty-state')
  })

  it('announces an error and lets the user retry', () => {
    const onRetry = vi.fn()
    const { rerender } = render(<AsyncErrorState message="Сервер не ответил" onRetry={onRetry} />)

    expect(screen.getByRole('alert')).toHaveTextContent('Сервер не ответил')
    fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
    expect(onRetry).toHaveBeenCalledOnce()

    rerender(<AsyncErrorState message="Сервер не ответил" onRetry={onRetry} retrying />)
    expect(screen.getByRole('button', { name: 'Загружаем…' })).toBeDisabled()
  })

  it('isolates a failed async section and can reset without crashing the application shell', () => {
    let shouldThrow = true
    const onError = vi.fn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    function UnstableSection() {
      if (shouldThrow) throw new Error('Не удалось загрузить фрагмент')
      return <p>Раздел восстановлен</p>
    }

    render(
      <AsyncErrorBoundary
        onError={onError}
        fallback={(error, reset) => (
          <div role="alert">
            <span>{error.message}</span>
            <button type="button" onClick={() => { shouldThrow = false; reset() }}>Повторить</button>
          </div>
        )}
      >
        <UnstableSection />
      </AsyncErrorBoundary>,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось загрузить фрагмент')
    expect(onError).toHaveBeenCalledTimes(1)
    fireEvent.click(screen.getByRole('button', { name: 'Повторить' }))
    expect(screen.getByText('Раздел восстановлен')).toBeInTheDocument()
    consoleError.mockRestore()
  })

  it('renders the shared table-shaped skeleton loader', () => {
    const { container } = render(<TableLoadingState label="Загружаем таблицу" />)

    expect(screen.getByRole('status', { name: 'Загружаем таблицу' })).toBeInTheDocument()
    expect(container.querySelectorAll('.loading-skeleton-row')).toHaveLength(4)
    expect(container.querySelectorAll('.loading-skeleton-line')).toHaveLength(16)
    expect(container.querySelector('.loading-skeleton-row')).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelector('.table-loading-state-spinner')).not.toBeInTheDocument()
  })
})
