import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { EmptyState, LoadingSkeleton } from './AsyncState'

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
})
