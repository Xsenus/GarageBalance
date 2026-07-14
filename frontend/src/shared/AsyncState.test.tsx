import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AsyncErrorBoundary, EmptyState, LoadingSkeleton, TableLoadingState } from './AsyncState'

describe('AsyncState', () => {
  it('announces loading without exposing decorative skeleton rows', () => {
    const { container } = render(<LoadingSkeleton label="Загружаем тарифы" rows={4} columns={3} />)

    expect(screen.getByRole('status', { name: 'Загружаем тарифы' })).toBeInTheDocument()
    expect(container.querySelectorAll('.loading-skeleton-row')).toHaveLength(4)
    expect(container.querySelectorAll('.loading-skeleton-line')).toHaveLength(12)
    expect(container.querySelector('.loading-skeleton-row')).toHaveAttribute('aria-hidden', 'true')
  })

  it('renders a polite spacious empty state', () => {
    render(<EmptyState>Данных пока нет.</EmptyState>)

    expect(screen.getByRole('status')).toHaveTextContent('Данных пока нет.')
    expect(screen.getByRole('status')).toHaveClass('empty-state--spacious')
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
